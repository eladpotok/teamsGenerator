import { FileTextOutlined, OneToOneOutlined, SendOutlined } from "@ant-design/icons";
import { Col, Divider, Modal, Popconfirm, Row } from "antd";
import { useState } from "react";
import Teams from "../Teams/Teams";

function MobileFooter(props) {
    const [teamsModalOpened, setTeamsModalOpened] = useState(false)

    return (
        <div style={{width: '100%'}} className={props.className} >
            <Row style={{ backgroundColor: 'white' , margin: '4px', borderRadius: '10px'}}>

                <Col
                    span={7}>
                    <Popconfirm onConfirm={props.onResetClicked} title='Reset all' description='Are you sure you want to clear all'>
                        <div style={{ verticalAlign: 'middle', textAlign: 'center', width: '100%', height: '65px', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                            <div style={{ display: 'flex', 'flex-direction': 'column', color: '#282c34' }}>
                                <FileTextOutlined style={{ fontSize: '24px', marginBottom: '4px' }} />
                                <label style={{ fontSize: '12px' }}>CLEAR</label>
                            </div>
                        </div>
                    </Popconfirm>
                </Col>

                <Col span={1}>
                    <Divider style={{ backgroundColor: "grey", height: '100%' }} type="vertical" />
                </Col>

                <Col span={7} >
                     <div style={{ verticalAlign: 'middle', textAlign: 'center', width: '100%', height: '65px', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                        { props.teams && <div onClick={() => {setTeamsModalOpened(true)}} style={{ display: 'flex', 'flex-direction': 'column', color: '#282c34' }}>
                            <OneToOneOutlined style={{ fontSize: '24px', marginBottom: '4px' }} />
                            <label style={{ fontSize: '12px' }}>SHOW RESULT</label>
                        </div>}
                        {!props.teams && <div  style={{ display: 'flex', 'flex-direction': 'column', color: '#bfbfbf' }}>
                            <OneToOneOutlined style={{ fontSize: '24px', marginBottom: '4px' }} />
                            <label style={{ fontSize: '12px' }}>SHOW RESULT</label>
                        </div>}
                    </div>
                </Col>

                <Col span={1}>
                    <Divider style={{ backgroundColor: "grey", height: '100%' }} type="vertical" />
                </Col>


                <Col span={8} >
                    {!props.teams && <div onClick={async () => {
                        const succeeded = await props.onGenerateTeams()
                        if (succeeded) setTeamsModalOpened(true)
                    }} style={{ textAlign: 'center', width: '100%', height: '100%', marginRight: '10px' }}>
                        <div style={{ verticalAlign: 'middle', textAlign: 'center', width: '100%', height: '65px', display: 'flex', alignItems: 'center', justifyContent: 'center', marginRight: '10px' }}>
                            {!props.teams && <div style={{ display: 'flex', 'flex-direction': 'column', color: '#282c34' }}>
                                <SendOutlined style={{ fontSize: '24px', marginBottom: '4px' }} />
                                <label style={{ fontSize: '12px' , marginRight: '10px'}}>GENERATE</label>
                            </div>}
                        </div>
                    </div>}

                    {props.teams &&  <Popconfirm title='Are you Sure?' description='Do you want to overwrite your last result?' onConfirm={async () => {
                            const succeeded = await props.onGenerateTeams()
                            if (succeeded) setTeamsModalOpened(true)
                        }}>
                        <div style={{ textAlign: 'center', width: '100%', height: '100%' , marginRight: '10px'}}>
                            <div style={{ verticalAlign: 'middle', textAlign: 'center', width: '100%', height: '65px', display: 'flex', alignItems: 'center', justifyContent: 'center', marginRight: '10px' }}>
                                {<div style={{ display: 'flex', 'flex-direction': 'column', color: '#282c34' }}>
                                    <SendOutlined style={{ fontSize: '24px', marginBottom: '4px'}} />
                                    <label style={{ fontSize: '12px' }}>GENERATE</label>
                                </div>}
                            </div>
                    </div></Popconfirm> }

                </Col>

                <Modal style={{margin: '10px'}} centered onCancel={() => { setTeamsModalOpened(false) }} open={teamsModalOpened} footer={[]}>
                    {props.teams && <Teams onMovePlayer={props.onMovePlayer} onRemovePlayerFromTeam={props.onRemovePlayer} teams={props.teams} onShuffleClicked={async () => {
                            const succeeded = await props.onGenerateTeams()
                        }}/>}
                </Modal>
            </Row>
        </div>

    )

}

export default MobileFooter;