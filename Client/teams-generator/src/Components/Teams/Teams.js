import { Button, Card, Col, Row, Select, message } from 'antd';
import { useContext } from 'react';
import { WhatsappShareButton, WhatsappIcon } from "react-share";
import { CopyOutlined } from '@ant-design/icons';
import Team from './Team';
import { getTextResult } from '../../Utilities/Helpers'
import { ConfigurationContext } from '../../Store/ConfigurationContext';
import {isMobile} from 'react-device-detect';
import { AnalyticsContext } from '../../Store/AnalyticsContext';


function Teams(props) {
    const [messageApi, contextHolder] = message.useMessage();
    const configContext = useContext(ConfigurationContext)

    const columnSpan = isMobile ? 24 : 8


    async function copyToClipboard() {
        const resultString = getTextResult(props.teams, configContext.userConfig.eventDate)
        navigator.clipboard.writeText(resultString)

        messageApi.open({
            type: 'success',
            content: 'Copied!',
        });
    }

    function getOptionalShirtsColors(currentTeamId){
        const shirtsOfOtherTeams = props.teams.filter(team => team.teamId !== currentTeamId).map(team => {
            return {
                label: team.teamSymbol,
                value: team.teamId
            }
        })

        // const restOfShirts = props.shirtsColors.filter( shirt => !props.teams.map( t => t.color.toUpperCase()).includes(shirt.toUpperCase()))
        
        // shirtsOfOtherTeams.push.apply(restOfShirts.map( t=> {return {   label: team.teamSymbol,
        //     value: team.teamId}}))

        return shirtsOfOtherTeams

    }

    function shirtColorChangedHandler(teamIdFrom, teamIdTo){
        props.onChangeShirtColor(teamIdFrom, teamIdTo)
    }


    return (
        <div>
            {contextHolder}
           
            <Row gutter={1}>
                <Col span={columnSpan} style={{marginTop: '-12px'}}>
                    <Card title={<div><Select  onChange={(value, label) => {shirtColorChangedHandler(props.teams[0].teamId, label.value )}} value={props.teams[0].teamSymbol} options={getOptionalShirtsColors(props.teams[0].teamId)}></Select> { 'Team ' + props.teams[0].teamName} </div>} bordered={false} size='small'>
                        <Team onMovePlayer={props.onMovePlayer} onRemovePlayerFromTeam={props.onRemovePlayerFromTeam} teams={props.teams} team={props.teams[0]} />
                    </Card>
                </Col>
                <Col span={columnSpan} style={{marginTop: '-12px'}}>
                    <Card title={<div><Select onChange={(value, label) => {shirtColorChangedHandler(props.teams[1].teamId, label.value )}} value={props.teams[1].teamSymbol} options={getOptionalShirtsColors(props.teams[1].teamId)}></Select> { 'Team ' + props.teams[1].teamName} </div>} bordered={false} size='small'>
                        <Team onMovePlayer={props.onMovePlayer} onRemovePlayerFromTeam={props.onRemovePlayerFromTeam} teams={props.teams} team={props.teams[1]} />
                    </Card>
                </Col>
                <Col span={columnSpan} style={{marginTop: '-12px'}}>
                    <Card title={<div><Select onChange={(value, label) => {shirtColorChangedHandler(props.teams[2].teamId, label.value )}} value={props.teams[2].teamSymbol} options={getOptionalShirtsColors(props.teams[2].teamId)}></Select> { 'Team ' + props.teams[1].teamName} </div>} bordered={false} size='small'>
                        <Team onMovePlayer={props.onMovePlayer} onRemovePlayerFromTeam={props.onRemovePlayerFromTeam} teams={props.teams} team={props.teams[2]} />
                    </Card>
                </Col>
            </Row>


            <div style={{ background: 'white', display: 'flex', 'flex-direction': 'row', 'align-items': 'center', 'justifyContent': 'flex-end' }}>

                <div style={{ background: 'white', display: 'flex', 'flex-direction': 'row', 'align-items': 'center', 'justifyContent': 'flex-end' }}>
                    <Button icon={<CopyOutlined />} onClick={copyToClipboard} style={{ borderRadius: '5px', marginTop: '5px', marginRight: '5px' }} />

                    <WhatsappShareButton url={getTextResult(props.teams, configContext.userConfig.eventDate)}>
                        <WhatsappIcon style={{ borderRadius: '5px', marginBottom: '-10px', marginRight: '5px' }} size={32} />
                    </WhatsappShareButton >
                </div>

                <div>
                    <Button onClick={() => { props.onShuffleClicked(configContext.algo) }} type="primary" style={{ borderRadius: '5px', marginTop: '5px', marginRight: '5px' }}>
                        Run Again
                    </Button>
                </div>

            </div>
        </div>
    );
}

export default Teams;