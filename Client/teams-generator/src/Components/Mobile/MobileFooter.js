import { FileTextOutlined, OneToOneOutlined, SendOutlined, SettingOutlined } from "@ant-design/icons";
import { Col, Divider, Dropdown, Modal, Popconfirm, Row } from "antd";
import { useState } from "react";
import Teams from "../Teams/Teams";
import MyIconButton from "../Common/MyIconButton";
import { RiTeamFill, RiUserAddFill } from "react-icons/ri";
import { RxHamburgerMenu } from "react-icons/rx";
import { AiOutlineClear } from "react-icons/ai";
import { HiEye } from "react-icons/hi";

function MobileFooter(props) {
    const [teamsModalOpened, setTeamsModalOpened] = useState(false)

    async function generateTeams() {
        const succeeded = await props.onGenerateTeams()
        if (succeeded) setTeamsModalOpened(true)
    }


    return (
        <div style={{ display: 'flex', 'flex-direction': 'row', 'align-items': 'flex-end', 'justifyContent': 'center', margin: '4px' }}>
      
            <div style={{marginRight: '18px', }}>
                <div>
                    <Popconfirm onConfirm={props.onResetClicked} title='Reset all' description='Are you sure you want to clear all'>
                        <MyIconButton text='CLEAR' buttonType='empty' icon={<AiOutlineClear style={{fontSize: '18px'}}/>}    />
                    </Popconfirm>
                </div>
            </div>
            <div style={{marginRight: '18px'}}>
                {!props.teams && <MyIconButton shadow onClick={generateTeams} text='RUN' circle icon={<RiTeamFill style={{ fontSize: '34px'}} />}/>}
                {props.teams && 
                <Popconfirm title='Are you Sure?' description='Do you want to overwrite your last result?' onConfirm={generateTeams}>
                    <MyIconButton shadow text='RUN' circle icon={<RiTeamFill style={{ fontSize: '34px'}} />}/>
                </Popconfirm>}
            </div>
            <div >
                {props.teams && <MyIconButton onClick={() => {setTeamsModalOpened(true)}}  text='SHOW' buttonType='empty' icon={<HiEye style={{fontSize: '18px'}}/>}    />}
                {!props.teams && <MyIconButton buttonType='empty' disabled   text='SHOW' icon={<HiEye style={{fontSize: '18px'}}/>}    />}
            </div>

            <Modal width='100%' style={{margin: '10px'}} centered onCancel={() => { setTeamsModalOpened(false) }} open={teamsModalOpened} footer={[]}>
                {props.teams && <Teams shirtsColors={props.shirtsColors} onChangeShirtColor={props.onChangeShirtColor} onMovePlayer={props.onMovePlayer} onRemovePlayerFromTeam={props.onRemovePlayerFromTeam} teams={props.teams} onShuffleClicked={async () => {
                        await props.onGenerateTeams()
                    }}/>}
            </Modal>
        
        </div>

    )

}

export default MobileFooter;